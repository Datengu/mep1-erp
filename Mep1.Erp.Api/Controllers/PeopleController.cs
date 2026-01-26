using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/people")]
[Authorize(Policy = "AdminOrOwner")]
public sealed class PeopleController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly AuditLogger _audit;

    public PeopleController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    private (int WorkerId, string Role, string Source) GetActorForAudit()
    {
        var id = ClaimsActor.GetWorkerId(User);
        var role = ClaimsActor.GetRole(User);
        return (id, role.ToString(), GetClientApp());
    }

    private string GetClientApp()
    {
        var kind = HttpContext.Items["ApiKeyKind"] as string;
        return string.Equals(kind, "Admin", StringComparison.Ordinal) ? "Desktop" : "Portal";
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

    private static DateTime AsUtcDate(DateTime d)
    {
        // dto.Date.Date produces Kind=Unspecified; Postgres timestamptz requires UTC
        return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
    }

    private static DateTime? AsUtcDateOrNull(DateTime? d)
    {
        return d.HasValue ? AsUtcDate(d.Value) : (DateTime?)null;
    }

    // ----------------------------
    // Portal Access (admin only)
    // ----------------------------

    [HttpGet("{workerId:int}/portal-access")]
    public async Task<ActionResult<PortalAccessDto>> GetPortalAccess(int workerId)
    {
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
    public async Task<ActionResult<CreatePortalAccessResultDto>> CreatePortalAccess(
        int workerId,
        [FromBody] CreatePortalAccessRequestDto request)
    {
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
        var normalized = NormalizeUsername(username);

        var usernameTaken = await _db.TimesheetUsers
            .AsNoTracking()
            .AnyAsync(u => u.UsernameNormalized == normalized);

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

        try
        {
            await _db.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // Unique index on UsernameNormalized is the source of truth
            return Conflict("Username is already taken.");
        }

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.PortalAccess.Create",
            entityType: "TimesheetUser",
            entityId: user.Id.ToString(),
            summary: $"Created portal access for WorkerId={workerId}, Username={username}, Role={role}"
        );

        var dto = new PortalAccessDto(
            Exists: true,
            WorkerId: user.WorkerId,
            Username: user.Username,
            Role: user.Role.ToString(),
            IsActive: user.IsActive,
            MustChangePassword: user.MustChangePassword,
            PasswordChangedAtUtc: user.PasswordChangedAtUtc
        );

        return Ok(new CreatePortalAccessResultDto(dto, tempPassword));
    }

    [HttpPatch("{workerId:int}/portal-access")]
    public async Task<IActionResult> UpdatePortalAccess(
        int workerId,
        [FromBody] UpdatePortalAccessRequestDto request)
    {
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

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.PortalAccess.Update",
            entityType: "TimesheetUser",
            entityId: user.Id.ToString(),
            summary: $"Updated portal access for WorkerId={workerId} (IsActive={user.IsActive}, Role={user.Role})"
        );

        return NoContent();
    }

    [HttpPost("{workerId:int}/portal-access/reset-password")]
    public async Task<ActionResult<ResetPortalPasswordResultDto>> ResetPortalPassword(int workerId)
    {
        var user = await _db.TimesheetUsers.FirstOrDefaultAsync(u => u.WorkerId == workerId);
        if (user is null)
            return NotFound("Portal account not found.");

        var tempPassword = GenerateTempPassword();
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(tempPassword);
        user.MustChangePassword = true;
        user.PasswordChangedAtUtc = DateTime.UtcNow; // “reset happened now”

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.PortalAccess.ResetPassword",
            entityType: "TimesheetUser",
            entityId: user.Id.ToString(),
            summary: $"Reset portal password for WorkerId={workerId}, Username={user.Username}"
        );

        return Ok(new ResetPortalPasswordResultDto(tempPassword));
    }

    [HttpGet("portal-access/username-available")]
    public async Task<ActionResult<PortalUsernameAvailableDto>> CheckPortalUsernameAvailable(
    [FromQuery] string username)
    {
        if (string.IsNullOrWhiteSpace(username))
            return BadRequest("Username is required.");

        var normalized = NormalizeUsername(username);

        // Check if already taken
        var exists = await _db.TimesheetUsers
            .AsNoTracking()
            .AnyAsync(u => u.UsernameNormalized == normalized);

        if (!exists)
        {
            return Ok(new PortalUsernameAvailableDto(
                Normalized: normalized,
                Available: true,
                Suggested: null
            ));
        }

        // Generate next free suggestion
        var suggested = await SuggestNextUsernameAsync(normalized);

        return Ok(new PortalUsernameAvailableDto(
            Normalized: normalized,
            Available: false,
            Suggested: suggested
        ));
    }

    [HttpGet("summary")]
    public ActionResult<List<PeopleSummaryRowDto>> GetPeopleSummary()
    {
        var rows = Reporting.GetPeopleSummary(_db);

        var dto = rows.Select(r => new PeopleSummaryRowDto
        {
            WorkerId = r.WorkerId,
            Initials = r.Initials,
            Name = r.Name,
            CurrentRatePerHour = r.CurrentRatePerHour,
            LastWorkedDate = r.LastWorkedDate,
            HoursThisMonth = r.HoursThisMonth,
            CostThisMonth = r.CostThisMonth,
            IsActive = r.IsActive
        }).ToList();

        return Ok(dto);
    }

    [HttpGet("{workerId:int}/drilldown")]
    public ActionResult<PersonDrilldownDto> GetPersonDrilldown(int workerId)
    {
        // Postgres timestamptz requires UTC DateTime parameters (Kind=Utc).
        var today = DateTime.UtcNow.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var rates = _db.WorkerRates
            .AsNoTracking()
            .Where(r => r.WorkerId == workerId)
            .OrderByDescending(r => r.ValidFrom)
            .Select(r => new WorkerRateDto
            {
                Id = r.Id,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo,
                RatePerHour = r.RatePerHour
            })
            .ToList();

        var projects = Reporting.GetPersonProjectBreakdown(_db, workerId, monthStart, today)
            .Select(p => new PersonProjectBreakdownRowDto
            {
                ProjectLabel = p.ProjectLabel,
                ProjectCode = p.ProjectCode,
                Hours = p.Hours,
                Cost = p.Cost
            })
            .ToList();

        var recent = Reporting.GetPersonRecentEntries(_db, workerId, take: 20)
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
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
            return NotFound("Worker not found.");

        worker.IsActive = request.IsActive;

        // NEW: if worker is deactivated, also deactivate their portal account (if it exists)
        if (!request.IsActive)
        {
            var user = await _db.TimesheetUsers.FirstOrDefaultAsync(u => u.WorkerId == workerId);
            if (user != null && user.IsActive)
            {
                user.IsActive = false;
            }
        }

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Worker.SetActive",
            entityType: "Worker",
            entityId: workerId.ToString(),
            summary: $"Set WorkerId={workerId} active={request.IsActive}"
        );

        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<CreateWorkerResponseDto>> CreateWorker([FromBody] CreateWorkerRequestDto req)
    {
        if (req == null) return BadRequest("Missing body.");

        var initials = (req.Initials ?? "").Trim();
        var name = (req.Name ?? "").Trim();

        if (string.IsNullOrWhiteSpace(initials))
            return BadRequest("Initials are required.");

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Name is required.");

        // Enforce unique initials
        var initialsTaken = await _db.Workers.AnyAsync(w => w.Initials == initials);
        if (initialsTaken) return BadRequest("Initials already exist.");

        var worker = new Worker
        {
            Initials = initials,
            Name = name,

            // Default to true unless explicitly provided
            IsActive = req.IsActive ?? true
        };

        _db.Workers.Add(worker);
        await _db.SaveChangesAsync();

        if (req.RatePerHour.HasValue)
        {
            var rate = req.RatePerHour.Value;
            if (rate < 0) return BadRequest("RatePerHour cannot be negative.");

            _db.WorkerRates.Add(new WorkerRate
            {
                WorkerId = worker.Id,
                RatePerHour = rate,
                ValidFrom = AsUtcDate(DateTime.UtcNow),
                ValidTo = null
            });

            await _db.SaveChangesAsync();
        }

        return Ok(new CreateWorkerResponseDto
        {
            Id = worker.Id,
            Initials = worker.Initials,
            Name = worker.Name,
            IsActive = worker.IsActive
        });
    }

    private static DateTime EndExclusive(DateTime? to)
        => (to ?? DateTime.MaxValue).Date;

    // Half-open ranges: [from, to) where to is exclusive (null = infinity)
    private static bool RangesOverlap(DateTime aFrom, DateTime? aTo, DateTime bFrom, DateTime? bTo)
    {
        var aStart = aFrom.Date;
        var bStart = bFrom.Date;

        var aEnd = EndExclusive(aTo);
        var bEnd = EndExclusive(bTo);

        // overlap iff starts before the other ends
        return aStart < bEnd && bStart < aEnd;
    }

    private async Task<string?> ValidateNoOverlapsForWorkerAsync(int workerId, DateTime newFrom, DateTime? newTo, int? ignoreRateId = null)
    {
        var rates = await _db.WorkerRates
            .AsNoTracking()
            .Where(r => r.WorkerId == workerId && (ignoreRateId == null || r.Id != ignoreRateId.Value))
            .ToListAsync();

        foreach (var r in rates)
        {
            if (RangesOverlap(newFrom, newTo, r.ValidFrom.Date, r.ValidTo?.Date))
                return $"Rate range overlaps an existing rate (Id={r.Id}, {r.ValidFrom:yyyy-MM-dd} to {(r.ValidTo.HasValue ? r.ValidTo.Value.ToString("yyyy-MM-dd") : "Current")}).";
        }

        return null;
    }

    private async Task<string?> ValidateSingleCurrentRateAsync(int workerId)
    {
        var currentCount = await _db.WorkerRates.CountAsync(r => r.WorkerId == workerId && r.ValidTo == null);
        if (currentCount != 1)
            return $"Worker must have exactly one current (open-ended) rate. Found {currentCount}.";
        return null;
    }

    [HttpGet("{workerId:int}/edit")]
    public async Task<ActionResult<WorkerForEditDto>> GetWorkerForEdit(int workerId)
    {
        var worker = await _db.Workers.AsNoTracking().FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
            return NotFound("Worker not found.");

        var rates = await _db.WorkerRates.AsNoTracking()
            .Where(r => r.WorkerId == workerId)
            .OrderByDescending(r => r.ValidFrom)
            .Select(r => new WorkerRateDto
            {
                Id = r.Id,
                ValidFrom = r.ValidFrom,
                ValidTo = r.ValidTo,
                RatePerHour = r.RatePerHour
            })
            .ToListAsync();

        return Ok(new WorkerForEditDto
        {
            WorkerId = worker.Id,
            Initials = worker.Initials,
            Name = worker.Name,
            SignatureName = worker.SignatureName,
            IsActive = worker.IsActive,
            Rates = rates
        });
    }

    [HttpPatch("{workerId:int}")]
    public async Task<IActionResult> UpdateWorkerDetails(int workerId, [FromBody] UpdateWorkerDetailsRequestDto req)
    {
        var worker = await _db.Workers.FirstOrDefaultAsync(w => w.Id == workerId);
        if (worker == null)
            return NotFound("Worker not found.");

        var initials = (req.Initials ?? "").Trim();
        var name = (req.Name ?? "").Trim();

        if (string.IsNullOrWhiteSpace(initials))
            return BadRequest("Initials are required.");

        if (string.IsNullOrWhiteSpace(name))
            return BadRequest("Name is required.");

        var initialsTaken = await _db.Workers.AnyAsync(w => w.Id != workerId && w.Initials == initials);
        if (initialsTaken)
            return Conflict("Initials already exist.");

        worker.Initials = initials;
        worker.Name = name;
        worker.SignatureName = string.IsNullOrWhiteSpace(req.SignatureName) ? null : req.SignatureName.Trim();

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Worker.UpdateDetails",
            entityType: "Worker",
            entityId: workerId.ToString(),
            summary: $"Updated WorkerId={workerId} (Initials={worker.Initials}, Name={worker.Name})"
        );

        return NoContent();
    }

    [HttpPost("{workerId:int}/rates/change-current")]
    public async Task<IActionResult> ChangeCurrentRate(int workerId, [FromBody] ChangeCurrentRateRequestDto req)
    {
        if (req.NewRatePerHour < 0m)
            return BadRequest("Rate cannot be negative.");

        var workerExists = await _db.Workers.AsNoTracking().AnyAsync(w => w.Id == workerId);
        if (!workerExists)
            return NotFound("Worker not found.");

        var singleCurrentErr = await ValidateSingleCurrentRateAsync(workerId);
        if (singleCurrentErr != null)
            return Conflict(singleCurrentErr);

        var current = await _db.WorkerRates
            .Where(r => r.WorkerId == workerId && r.ValidTo == null)
            .FirstAsync();

        var effectiveFrom = AsUtcDate(req.EffectiveFrom);

        if (effectiveFrom < current.ValidFrom.Date)
            return BadRequest($"EffectiveFrom must be on/after current rate ValidFrom ({current.ValidFrom:yyyy-MM-dd}).");

        // If effectiveFrom is the same day the current starts, we just replace it (no split)
        if (effectiveFrom == current.ValidFrom.Date)
        {
            current.RatePerHour = req.NewRatePerHour;
            await _db.SaveChangesAsync();

            var a0 = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a0.WorkerId,
                subjectWorkerId: a0.WorkerId,
                actorRole: a0.Role,
                actorSource: a0.Source,
                action: "People.Rates.UpdateCurrentAmount",
                entityType: "WorkerRate",
                entityId: current.Id.ToString(),
                summary: $"Updated current rate amount WorkerId={workerId}, RateId={current.Id}, NewRate={req.NewRatePerHour}"
            );

            return NoContent();
        }

        // Split (exclusive-end)
        // Old current becomes historical [oldFrom, effectiveFrom)
        // New rate becomes current [effectiveFrom, null)

        // Ensure the new open-ended rate won't overlap anything else
        // Ignore the current rate because we're splitting it in the same operation.
        var overlapErr = await ValidateNoOverlapsForWorkerAsync(workerId, effectiveFrom, null, ignoreRateId: current.Id);
        if (overlapErr != null)
            return Conflict(overlapErr);

        // Exclusive end: old rate ends at EffectiveFrom (so it applies up to the day before)
        current.ValidTo = effectiveFrom;

        var newRate = new WorkerRate
        {
            WorkerId = workerId,
            RatePerHour = req.NewRatePerHour,
            ValidFrom = effectiveFrom,
            ValidTo = null
        };

        _db.WorkerRates.Add(newRate);
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Rates.ChangeCurrent",
            entityType: "Worker",
            entityId: workerId.ToString(),
            summary: $"Split current rate (old RateId={current.Id} now ends {current.ValidTo:yyyy-MM-dd}); created new current rate RateId={newRate.Id} from {newRate.ValidFrom:yyyy-MM-dd} at {newRate.RatePerHour}"
        );

        return NoContent();
    }

    [HttpPost("{workerId:int}/rates")]
    public async Task<IActionResult> AddHistoricalRate(int workerId, [FromBody] AddWorkerRateRequestDto req)
    {
        if (req.RatePerHour < 0m)
            return BadRequest("Rate cannot be negative.");

        var from = AsUtcDate(req.ValidFrom);
        var to = AsUtcDate(req.ValidTo);

        if (to <= from)
            return BadRequest("ValidTo must be after ValidFrom (ValidTo is exclusive).");

        var workerExists = await _db.Workers.AsNoTracking().AnyAsync(w => w.Id == workerId);
        if (!workerExists)
            return NotFound("Worker not found.");

        var overlapErr = await ValidateNoOverlapsForWorkerAsync(workerId, from, to);
        if (overlapErr != null)
            return Conflict(overlapErr);

        var rate = new WorkerRate
        {
            WorkerId = workerId,
            RatePerHour = req.RatePerHour,
            ValidFrom = from,
            ValidTo = to
        };

        _db.WorkerRates.Add(rate);
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Rates.AddHistorical",
            entityType: "WorkerRate",
            entityId: rate.Id.ToString(),
            summary: $"Added historical rate WorkerId={workerId}, RateId={rate.Id}, {from:yyyy-MM-dd} to {to:yyyy-MM-dd} at {rate.RatePerHour}"
        );

        return NoContent();
    }

    [HttpPatch("{workerId:int}/rates/{rateId:int}")]
    public async Task<IActionResult> UpdateRateAmount(int workerId, int rateId, [FromBody] UpdateWorkerRateAmountRequestDto req)
    {
        if (req.RatePerHour < 0m)
            return BadRequest("Rate cannot be negative.");

        var rate = await _db.WorkerRates.FirstOrDefaultAsync(r => r.Id == rateId && r.WorkerId == workerId);
        if (rate == null)
            return NotFound("Rate not found.");

        rate.RatePerHour = req.RatePerHour;
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Rates.UpdateAmount",
            entityType: "WorkerRate",
            entityId: rateId.ToString(),
            summary: $"Updated rate amount WorkerId={workerId}, RateId={rateId}, NewRate={req.RatePerHour}"
        );

        return NoContent();
    }

    [HttpDelete("{workerId:int}/rates/{rateId:int}")]
    public async Task<IActionResult> DeleteRate(int workerId, int rateId)
    {
        var rate = await _db.WorkerRates.FirstOrDefaultAsync(r => r.Id == rateId && r.WorkerId == workerId);
        if (rate == null)
            return NotFound("Rate not found.");

        // Don't allow deleting the current rate at all (too risky / can break invariants)
        if (rate.ValidTo == null)
            return Conflict("Cannot delete the current rate. Use 'Schedule new rate' to correct mistakes explicitly.");

        var start = AsUtcDate(rate.ValidFrom);
        var endExclusive = rate.ValidTo.HasValue
            ? AsUtcDate(rate.ValidTo.Value)
            : DateTime.SpecifyKind(DateTime.MaxValue.Date, DateTimeKind.Utc);

        var hasEntries = await _db.TimesheetEntries
            .AsNoTracking()
            .AnyAsync(e => e.WorkerId == workerId
                          && !e.IsDeleted
                          && e.Date.Date >= start
                          && e.Date.Date < endExclusive);

        if (hasEntries)
            return Conflict("Cannot delete a rate that covers existing timesheet entries. Adjust via explicit rate changes instead.");

        _db.WorkerRates.Remove(rate);
        await _db.SaveChangesAsync();

        // After delete, ensure there is still exactly one current rate
        var currentCount = await _db.WorkerRates.CountAsync(r => r.WorkerId == workerId && r.ValidTo == null);
        if (currentCount != 1)
            return Conflict("Delete would leave worker without exactly one current rate. Aborting.");

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "People.Rates.Delete",
            entityType: "WorkerRate",
            entityId: rateId.ToString(),
            summary: $"Deleted rate WorkerId={workerId}, RateId={rateId}, {start:yyyy-MM-dd} to {(rate.ValidTo.HasValue ? rate.ValidTo.Value.ToString("yyyy-MM-dd") : "Current")}"
        );

        return NoContent();
    }

    private static string NormalizeUsername(string input)
    {
        var trimmed = input.Trim().ToLowerInvariant();

        // letters, digits, dots only
        var chars = trimmed
            .Where(c => char.IsLetterOrDigit(c) || c == '.')
            .ToArray();

        return new string(chars);
    }

    private async Task<string> SuggestNextUsernameAsync(string baseNormalized)
    {
        // Pull all usernames that start with the base
        var taken = await _db.TimesheetUsers
            .AsNoTracking()
            .Where(u => u.UsernameNormalized.StartsWith(baseNormalized))
            .Select(u => u.UsernameNormalized)
            .ToListAsync();

        if (!taken.Contains(baseNormalized))
            return baseNormalized;

        for (int i = 2; i < 1000; i++)
        {
            var candidate = baseNormalized + i;
            if (!taken.Contains(candidate))
                return candidate;
        }

        throw new InvalidOperationException("Unable to generate username.");
    }
}