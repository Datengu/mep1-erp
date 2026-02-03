using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using ApplicationEntity = Mep1.Erp.Core.Application;

namespace Mep1.Erp.Api.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize(Policy = "AdminOrOwner")]
    public sealed class ApplicationsController : ControllerBase
    {
        private readonly AppDbContext _db;
        private readonly AuditLogger _audit;

        public ApplicationsController(AppDbContext db, AuditLogger audit)
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

        // GET api/applications
        [HttpGet]
        public async Task<ActionResult<List<ApplicationListEntryDto>>> GetApplications()
        {
            // Keep it simple and explicit (no Reporting helper yet)
            var apps = await _db.Applications
                .AsNoTracking()
                .Include(a => a.Project)
                    .ThenInclude(p => p.CompanyEntity)
                .OrderBy(a => a.ProjectCode)
                .ThenBy(a => a.ApplicationNumber)
                .ToListAsync();

            // VAT is not stored on Application (yet) - mirror Invoice UX by computing it.
            const decimal defaultVatRate = 0.20m;

            var dto = apps.Select(a =>
            {
                var (vatAmount, grossAmount) = ComputeVat(a.SubmittedNetAmount, defaultVatRate);

                return new ApplicationListEntryDto
                {
                    Id = a.Id,
                    ApplicationNumber = FormatApplicationRef(a.ApplicationNumber),
                    ProjectCode = a.ProjectCode,
                    JobName = a.Project?.JobNameOrNumber ?? "",
                    ClientName = a.Project?.CompanyEntity?.Name ?? "",
                    Status = a.Status ?? "",
                    ApplicationDate = a.DateApplied,
                    NetAmount = a.SubmittedNetAmount,
                    GrossAmount = grossAmount,
                    AgreedNetAmount = a.AgreedNetAmount,
                    DateAgreed = a.DateAgreed,
                    Notes = a.Notes ?? "",
                    InvoiceId = _db.Invoices
                        .Where(i => i.ApplicationId == a.Id)
                        .Select(i => (int?)i.Id)
                        .FirstOrDefault()
                };
            }).ToList();

            return Ok(dto);
        }

        // GET api/applications/project-picklist
        // (keeps the same UX pattern as invoices, but without touching ProjectsController)
        [HttpGet("project-picklist")]
        public async Task<ActionResult<List<ApplicationProjectPicklistItemDto>>> GetProjectPicklist()
        {
            var projects = await _db.Projects
                .AsNoTracking()
                .Include(p => p.CompanyEntity)
                .OrderBy(p => p.JobNameOrNumber)
                .ToListAsync();

            var dto = projects.Select(p => new ApplicationProjectPicklistItemDto
            {
                ProjectId = p.Id,
                JobNameOrNumber = p.JobNameOrNumber ?? "",
                CompanyName = p.CompanyEntity?.Name ?? ""
            }).ToList();

            return Ok(dto);
        }

        // POST api/applications
        [HttpPost]
        public async Task<ActionResult<CreateApplicationResponseDto>> Create([FromBody] CreateApplicationRequestDto dto)
        {
            var appNo = ParseApplicationNumber(dto.ApplicationNumber);
            if (appNo <= 0)
                return BadRequest("Application number must be a positive integer.");

            var project = await _db.Projects
                .Include(p => p.CompanyEntity)
                .FirstOrDefaultAsync(p => p.Id == dto.ProjectId);

            if (project == null)
                return BadRequest("Project not found.");

            if (project.CompanyId == null || project.CompanyEntity == null)
                return BadRequest("Selected project has no company linked.");

            if (dto.NetAmount <= 0m)
                return BadRequest("Net amount must be > 0.");

            var vatRate = dto.VatRate;
            if (vatRate < 0m || vatRate > 1m)
                return BadRequest("VAT rate must be between 0 and 1 (e.g. 0.2 for 20%).");

            var projectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
            var exists = await _db.Applications
                .AsNoTracking()
                .AnyAsync(a => a.ProjectCode == projectCode && a.ApplicationNumber == appNo);

            if (exists)
                return Conflict($"Application '{FormatApplicationRef(appNo)}' already exists for project '{projectCode}'.");

            var status = CleanStatus(dto.Status);

            var entity = new ApplicationEntity
            {
                ProjectId = project.Id,
                ProjectCode = projectCode,

                ApplicationNumber = appNo,
                DateApplied = AsUtcDate(dto.ApplicationDate),

                SubmittedNetAmount = dto.NetAmount,

                AgreedNetAmount = dto.AgreedNetAmount,
                DateAgreed = dto.DateAgreed == null ? null : AsUtcDate(dto.DateAgreed.Value),

                Status = status,
                Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
            };

            _db.Applications.Add(entity);
            await _db.SaveChangesAsync();

            var (vatAmount, grossAmount) = ComputeVat(entity.SubmittedNetAmount, vatRate);

            var a = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a.WorkerId,
                subjectWorkerId: a.WorkerId,
                actorRole: a.Role,
                actorSource: a.Source,
                action: "Application.Create",
                entityType: "Application",
                entityId: entity.Id.ToString(),
                summary: $"{entity.ProjectCode} App {entity.ApplicationNumber} {entity.SubmittedNetAmount:0.00}net {entity.Status}"
            );

            var resp = new CreateApplicationResponseDto
            {
                Id = entity.Id,
                ApplicationNumber = FormatApplicationRef(entity.ApplicationNumber),

                ProjectId = project.Id,
                ProjectCode = projectCode,
                JobNameOrNumber = project.JobNameOrNumber ?? "",
                CompanyName = project.CompanyEntity!.Name,

                ApplicationDate = entity.DateApplied,

                NetAmount = entity.SubmittedNetAmount,
                VatRate = vatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                AgreedNetAmount = entity.AgreedNetAmount,
                DateAgreed = entity.DateAgreed,

                Status = entity.Status,
                Notes = entity.Notes
            };

            return Ok(resp);
        }

        // GET api/applications/{id}
        [HttpGet("{id:int}")]
        public async Task<ActionResult<ApplicationDetailsDto>> GetById([FromRoute] int id)
        {
            var a = await _db.Applications
                .AsNoTracking()
                .Include(x => x.Project)
                    .ThenInclude(p => p.CompanyEntity)
                .FirstOrDefaultAsync(x => x.Id == id);

            if (a == null)
                return NotFound();

            const decimal defaultVatRate = 0.20m;
            var (vatAmount, grossAmount) = ComputeVat(a.SubmittedNetAmount, defaultVatRate);

            var resp = new ApplicationDetailsDto
            {
                Id = a.Id,
                ApplicationNumber = FormatApplicationRef(a.ApplicationNumber),

                ProjectId = a.ProjectId,
                ProjectCode = a.ProjectCode,
                JobName = a.Project?.JobNameOrNumber,
                CompanyName = a.Project?.CompanyEntity?.Name,

                ApplicationDate = a.DateApplied,

                NetAmount = a.SubmittedNetAmount,
                VatRate = defaultVatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                Status = a.Status,
                Notes = a.Notes,

                AgreedNetAmount = a.AgreedNetAmount,
                DateAgreed = a.DateAgreed,
            };

            return Ok(resp);
        }

        // PUT api/applications/{id}
        [HttpPut("{id:int}")]
        public async Task<ActionResult<UpdateApplicationResponseDto>> Update([FromRoute] int id, [FromBody] UpdateApplicationRequestDto dto)
        {
            var entity = await _db.Applications
                .Include(a => a.Project)
                    .ThenInclude(p => p.CompanyEntity)
                .FirstOrDefaultAsync(a => a.Id == id);

            if (entity == null)
                return NotFound();

            if (dto.NetAmount <= 0m)
                return BadRequest("Net amount must be > 0.");

            var vatRate = dto.VatRate;
            if (vatRate < 0m || vatRate > 1m)
                return BadRequest("VAT rate must be between 0 and 1 (e.g. 0.2 for 20%).");

            // Optional project re-link
            Project? project = null;
            if (dto.ProjectId != null)
            {
                project = await _db.Projects
                    .Include(p => p.CompanyEntity)
                    .FirstOrDefaultAsync(p => p.Id == dto.ProjectId.Value);

                if (project == null)
                    return BadRequest("Project not found.");

                if (project.CompanyId == null || project.CompanyEntity == null)
                    return BadRequest("Selected project has no company linked.");

                entity.ProjectId = project.Id;
                entity.ProjectCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);
            }

            entity.DateApplied = AsUtcDate(dto.ApplicationDate);

            entity.SubmittedNetAmount = dto.NetAmount;
            entity.Status = CleanStatus(dto.Status);
            entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

            // Future-safe fields
            entity.AgreedNetAmount = dto.AgreedNetAmount;
            entity.DateAgreed = dto.DateAgreed == null ? null : AsUtcDate(dto.DateAgreed.Value);

            await _db.SaveChangesAsync();

            // Ensure project is loaded for response
            if (entity.Project == null)
            {
                entity = await _db.Applications
                    .AsNoTracking()
                    .Include(a => a.Project)
                        .ThenInclude(p => p.CompanyEntity)
                    .FirstAsync(a => a.Id == id);
            }

            var (vatAmount, grossAmount) = ComputeVat(entity.SubmittedNetAmount, vatRate);

            var actor = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: actor.WorkerId,
                subjectWorkerId: actor.WorkerId,
                actorRole: actor.Role,
                actorSource: actor.Source,
                action: "Application.Update",
                entityType: "Application",
                entityId: entity.Id.ToString(),
                summary: $"{entity.ProjectCode} App {entity.ApplicationNumber} {entity.SubmittedNetAmount:0.00}net {entity.Status}"
            );

            var resp = new UpdateApplicationResponseDto
            {
                Id = entity.Id,
                ApplicationNumber = FormatApplicationRef(entity.ApplicationNumber),

                ProjectId = entity.ProjectId,
                ProjectCode = entity.ProjectCode,
                JobName = entity.Project?.JobNameOrNumber,
                CompanyName = entity.Project?.CompanyEntity?.Name,

                ApplicationDate = entity.DateApplied,

                NetAmount = entity.SubmittedNetAmount,
                VatRate = vatRate,
                VatAmount = vatAmount,
                GrossAmount = grossAmount,

                Status = entity.Status,
                Notes = entity.Notes,

                AgreedNetAmount = entity.AgreedNetAmount,
                DateAgreed = entity.DateAgreed,
            };

            return Ok(resp);
        }

        private static int ParseApplicationNumber(string? s)
        {
            s = (s ?? "").Trim();

            if (string.IsNullOrWhiteSpace(s))
                return 0;

            // Allow "APP001", "app1", "APP 002"
            s = s.Replace(" ", "", StringComparison.OrdinalIgnoreCase);

            if (s.StartsWith("APP", StringComparison.OrdinalIgnoreCase))
                s = s.Substring(3);

            // Now s should be digits (leading zeros allowed)
            if (!int.TryParse(s, out var n))
                return 0;

            return n;
        }

        private static string FormatApplicationRef(int n)
        {
            // D3 => 1 => 001, 12 => 012, 999 => 999, 1000 => 1000
            return $"APP{n.ToString("D3", System.Globalization.CultureInfo.InvariantCulture)}";
        }

        private static (decimal VatAmount, decimal GrossAmount) ComputeVat(decimal net, decimal vatRate)
        {
            var vatAmount = Math.Round(net * vatRate, 2, MidpointRounding.AwayFromZero);
            var gross = net + vatAmount;
            return (vatAmount, gross);
        }

        private static DateTime AsUtcDate(DateTime dt)
        {
            // Mirrors invoice pattern: treat as "date", normalize kind
            return DateTime.SpecifyKind(dt.Date, DateTimeKind.Utc);
        }

        private static string CleanStatus(string? s)
        {
            s = (s ?? "").Trim();
            return string.IsNullOrWhiteSpace(s) ? "Submitted" : s;
        }
    }
}