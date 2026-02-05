using DocumentFormat.OpenXml.Drawing;
using Mep1.Erp.Api.Security;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;
using System.Linq;
using System.Globalization;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Policy = "AdminOrOwner")]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly AuditLogger _audit;
    private readonly IMemoryCache _cache;

    public ProjectsController(AppDbContext db, AuditLogger audit, IMemoryCache cache)
    {
        _db = db;
        _audit = audit;
        _cache = cache;
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

    private static DateTime AsUtcDate(DateTime d)
    {
        // dto.Date.Date produces Kind=Unspecified; Postgres timestamptz requires UTC
        return DateTime.SpecifyKind(d.Date, DateTimeKind.Utc);
    }

    private static DateTime? AsUtcDateOrNull(DateTime? d)
    {
        return d.HasValue ? AsUtcDate(d.Value) : (DateTime?)null;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.JobNameOrNumber)
            .Select(p => new 
            {
                Id = p.Id,
                JobNameOrNumber = p.JobNameOrNumber,
                IsActive = p.IsActive,

                CompanyId = p.CompanyId,
                CompanyCode = p.CompanyEntity != null ? p.CompanyEntity.Code : null,
                CompanyName = p.CompanyEntity != null ? p.CompanyEntity.Name : null
            })
            .ToListAsync();

        return Ok(projects);
    }

    [HttpGet("summary")]
    public ActionResult<List<ProjectSummaryDto>> GetSummary()
    {
        const string cacheKey = "projects.summary.v1";
        if (_cache.TryGetValue(cacheKey, out List<ProjectSummaryDto>? cached) && cached != null)
            return Ok(cached);

        var rows = Reporting.GetProjectCostVsInvoiced(_db);

        // map JobNameOrNumber -> IsActive from DB
        var activeByJob = _db.Projects
            .AsNoTracking()
            .Where(p => p.IsRealProject)
            .Select(p => new { p.JobNameOrNumber, p.IsActive })
            .ToDictionary(x => x.JobNameOrNumber, x => x.IsActive);

        var dto = rows.Select(p => new ProjectSummaryDto
        {
            JobNameOrNumber = p.JobNameOrNumber,
            BaseCode = p.BaseCode,
            IsActive = activeByJob.TryGetValue(p.JobNameOrNumber, out var isActive) && isActive,
            LabourCost = p.LabourCost,
            SupplierCost = p.SupplierCost,
            TotalCost = p.TotalCost,
            InvoicedNet = p.InvoicedNet,
            InvoicedGross = p.InvoicedGross,
            ProfitNet = p.ProfitNet,
            ProfitGross = p.ProfitGross
        })
        .OrderBy(x => x.JobNameOrNumber)
        .ToList();

        // Cache for a short time (v1 pragmatic optimisation)
        _cache.Set(cacheKey, dto, new MemoryCacheEntryOptions
        {
            AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(20)
        });

        return Ok(dto);
    }

    [HttpPost]
    public async Task<ActionResult<CreateProjectResponseDto>> Create([FromBody] CreateProjectRequestDto dto)
    {
        var job = (dto.JobNameOrNumber ?? "").Trim();
        if (string.IsNullOrWhiteSpace(job))
            return BadRequest("Job name / number is required.");

        var companyCode = (dto.CompanyCode ?? "").Trim().ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(companyCode))
            return BadRequest("CompanyCode is required.");

        // Optional: reject legacy pseudo-companies at API level too
        // (you already do it in importer, but this prevents UI creating them)
        var nonCompanyCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "HOL", "SICK", "TP", "MEP1" };
        if (nonCompanyCodes.Contains(companyCode))
            return BadRequest($"'{companyCode}' is not a real company code.");

        // Ensure Company exists (create if missing)
        var company = await _db.Companies.SingleOrDefaultAsync(c => c.Code == companyCode);
        if (company == null)
        {
            company = new Company
            {
                Code = companyCode,
                Name = companyCode,   // you can improve this later
                IsActive = true
            };

            _db.Companies.Add(company);
            await _db.SaveChangesAsync();
        }

        // Uniqueness check (keep your old behaviour)
        var exists = await _db.Projects.AnyAsync(p => p.JobNameOrNumber == job);
        if (exists)
            return Conflict($"Project '{job}' already exists.");

        var project = new Project
        {
            JobNameOrNumber = job,
            CompanyId = company.Id,
            IsActive = dto.IsActive,
            Category = "Project",
            IsRealProject = true,
            CompanyEntity = company
        };


        _db.Projects.Add(project);
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.Create",
            entityType: "Project",
            entityId: project.Id.ToString(),
            summary: $"Job={project.JobNameOrNumber}, CompanyId={project.CompanyId}, CompanyName={project.CompanyEntity?.Name ?? ""}, IsActive={project.IsActive}"
        );

        var resp = new CreateProjectResponseDto
        {
            Id = project.Id,
            JobNameOrNumber = project.JobNameOrNumber,
            IsActive = project.IsActive,

            CompanyId = project.CompanyId,
            CompanyCode = company.Code,
            CompanyName = company.Name
        };

        // We don't have a dedicated GET-by-id route here, so use Created with a reasonable location
        return Created($"api/projects/{Uri.EscapeDataString(project.JobNameOrNumber)}/drilldown", resp);
    }

    [HttpGet("{jobKey}/drilldown")]
    public async Task<ActionResult<ProjectDrilldownDto>> GetDrilldown([FromRoute] string jobKey, [FromQuery] int recentTake = 25)
    {
        var project = await _db.Projects
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);

        if (project == null)
            return NotFound();

        var baseCode = ProjectCodeHelpers.GetBaseProjectCode(project.JobNameOrNumber);

        // Postgres timestamptz requires UTC DateTime parameters (Kind=Utc).
        var today = AsUtcDate(DateTime.UtcNow);
        var monthStart = new DateTime(today.Year, today.Month, 1, 0, 0, 0, DateTimeKind.Utc);

        var labourThisMonth = Reporting.GetProjectLabourByPerson(_db, project.Id, monthStart, today)
            .Select(x => new ProjectLabourByPersonRowDto(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
            .ToList();

        var labourAllTime = Reporting.GetProjectLabourByPerson(_db, project.Id)
            .Select(x => new ProjectLabourByPersonRowDto(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
            .ToList();

        var recentEntries = Reporting.GetProjectRecentEntries(_db, project.Id, take: recentTake)
            .Select(x => new ProjectRecentEntryRowDto(x.Date, x.WorkerInitials, x.Hours, x.Cost, x.TaskDescription))
            .ToList();

        var invoices = Reporting.GetInvoiceList(_db)
            .Where(x =>
                x.ProjectId == project.Id
                || (x.ProjectId == null && baseCode != null && x.ProjectCode == baseCode))
            .OrderByDescending(x => x.InvoiceDate)
            .ThenByDescending(x => x.InvoiceNumber)
            .Select(x => new ProjectInvoiceRowDto(
                x.InvoiceNumber,
                x.InvoiceDate,
                x.DueDate,
                x.NetAmount,
                x.OutstandingNet,
                x.Status,
                x.PaymentAmount,
                x.PaidDate))
            .ToList();

        var appsRaw = await _db.Applications
            .AsNoTracking()
            .Where(a => a.ProjectCode == baseCode)
            .OrderByDescending(a => a.DateApplied)
            .ThenByDescending(a => a.ApplicationNumber)
            .Select(a => new
            {
                a.ProjectCode,
                a.ApplicationNumber,
                a.DateApplied,
                a.SubmittedNetAmount,

                InvoiceNumber = a.Invoice != null ? a.Invoice.InvoiceNumber : null,
                InvoiceDate = a.Invoice != null ? (DateTime?)a.Invoice.InvoiceDate : null,
                InvoiceNet = a.Invoice != null ? (decimal?)a.Invoice.NetAmount : null,

                PaymentGross = a.Invoice != null ? a.Invoice.PaymentAmount : null,
                PaidDate = a.Invoice != null ? a.Invoice.PaidDate : null,

                VatRate = a.Invoice != null ? a.Invoice.VatRate : null,
                InvoiceGross = a.Invoice != null ? a.Invoice.GrossAmount : null
            })
            .ToListAsync();

        var applications = appsRaw
            .Select(a => new ProjectApplicationRowDto(
                a.ProjectCode,
                a.ApplicationNumber,
                a.DateApplied,
                a.SubmittedNetAmount,

                a.InvoiceNumber,
                a.InvoiceDate,
                a.InvoiceNet,

                a.PaymentGross,
                DerivePaymentNet(a.PaymentGross, a.VatRate, a.InvoiceNet, a.InvoiceGross),
                a.PaidDate
            ))
            .ToList();

        var supplierCosts = await _db.SupplierCosts
            .AsNoTracking()
            .Where(sc => sc.ProjectId == project.Id)
            .OrderByDescending(sc => sc.Date.HasValue)
            .ThenByDescending(sc => sc.Date)
            .ThenByDescending(sc => sc.Id)
            .Select(sc => new SupplierCostRowDto(
                sc.Id,
                sc.Date,
                sc.SupplierId,
                sc.Supplier.Name,
                sc.Amount,
                sc.Note))
            .ToListAsync();

        var dto = new ProjectDrilldownDto(
            JobNameOrNumber: project.JobNameOrNumber,
            BaseCode: baseCode,
            LabourThisMonth: labourThisMonth,
            LabourAllTime: labourAllTime,
            RecentEntries: recentEntries,
            Applications: applications,
            Invoices: invoices,
            SupplierCosts: supplierCosts);

        return Ok(dto);
    }

    [HttpPost("{jobKey}/supplier-costs")]
    public async Task<ActionResult<SupplierCostRowDto>> AddSupplierCost([FromRoute] string jobKey, [FromBody] UpsertSupplierCostDto dto)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound();

        if (dto.Amount <= 0m) return BadRequest("Amount must be > 0.");

        var supplier = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == dto.SupplierId);
        if (supplier == null) return BadRequest("Supplier not found.");

        var entity = new SupplierCost
        {
            ProjectId = project.Id,
            SupplierId = dto.SupplierId,
            Date = AsUtcDateOrNull(dto.Date),
            Amount = dto.Amount,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()
        };

        _db.SupplierCosts.Add(entity);
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.SupplierCost.Create",
            entityType: "SupplierCost",
            entityId: entity.Id.ToString(),
            summary: $"Job={jobKey}, SupplierId={dto.SupplierId}, Amount={dto.Amount}, Date={(dto.Date?.Date.ToString("yyyy-MM-dd") ?? "null")}"
        );

        return Ok(new SupplierCostRowDto(entity.Id, entity.Date, entity.SupplierId, supplier.Name, entity.Amount, entity.Note));
    }

    [HttpPut("{jobKey}/supplier-costs/{id:int}")]
    public async Task<IActionResult> UpdateSupplierCost([FromRoute] string jobKey, [FromRoute] int id, [FromBody] UpsertSupplierCostDto dto)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound();

        var entity = await _db.SupplierCosts.FirstOrDefaultAsync(sc => sc.Id == id && sc.ProjectId == project.Id);
        if (entity == null) return NotFound();

        if (dto.Amount <= 0m) return BadRequest("Amount must be > 0.");

        var supplierExists = await _db.Suppliers.AnyAsync(s => s.Id == dto.SupplierId);
        if (!supplierExists) return BadRequest("Supplier not found.");

        entity.SupplierId = dto.SupplierId;
        entity.Date = AsUtcDateOrNull(dto.Date);
        entity.Amount = dto.Amount;
        entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.SupplierCost.Update",
            entityType: "SupplierCost",
            entityId: entity.Id.ToString(),
            summary: $"Job={jobKey}, SupplierId={entity.SupplierId}, Amount={entity.Amount}, Date={(entity.Date?.ToString("yyyy-MM-dd") ?? "null")}"
        );

        return NoContent();
    }

    [HttpDelete("{jobKey}/supplier-costs/{id:int}")]
    public async Task<IActionResult> DeleteSupplierCost([FromRoute] string jobKey, [FromRoute] int id)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound();

        var entity = await _db.SupplierCosts.FirstOrDefaultAsync(sc => sc.Id == id && sc.ProjectId == project.Id);
        if (entity == null) return NotFound();

        var deletedId = entity.Id;

        _db.SupplierCosts.Remove(entity);
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.SupplierCost.Delete",
            entityType: "SupplierCost",
            entityId: deletedId.ToString(),
            summary: $"Job={jobKey}, SupplierCostId={deletedId}"
        );

        return NoContent();
    }

    [HttpPatch("{jobKey}/active")]
    public async Task<IActionResult> SetProjectActive([FromRoute] string jobKey, [FromBody] SetProjectActiveDto dto)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);

        if (project == null) 
            return NotFound();

        // prevent disabling non-real/system rows
        if (!project.IsRealProject)
            return BadRequest("System projects cannot be activated/deactivated.");

        project.IsActive = dto.IsActive;
        await _db.SaveChangesAsync();

        _cache.Remove("projects.summary.v1");

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.SetActive",
            entityType: "Project",
            entityId: project.Id.ToString(),
            summary: $"Job={jobKey}, IsActive={dto.IsActive}"
        );

        return NoContent();
    }

    // Used by Desktop "Add Invoice" to select a project and derive company
    [HttpGet("picklist/invoices")]
    public async Task<ActionResult<List<InvoiceProjectPicklistItemDto>>> GetInvoicePicklist()
    {
        var rows = await _db.Projects
            .AsNoTracking()
            .Where(p => p.IsRealProject && p.CompanyId != null)
            .OrderBy(p => p.JobNameOrNumber)
            .Select(p => new InvoiceProjectPicklistItemDto
            {
                ProjectId = p.Id,
                JobNameOrNumber = p.JobNameOrNumber,

                CompanyId = p.CompanyId!.Value,
                CompanyName = p.CompanyEntity != null ? p.CompanyEntity.Name : ""
            })
            .ToListAsync();

        return Ok(rows);
    }

    [HttpGet("{projectId:int}/ccf-refs")]
    public async Task<IActionResult> GetProjectCcfRefs([FromRoute] int projectId, [FromQuery] bool includeInactive = false)
    {
        var projectExists = await _db.Projects.AnyAsync(p => p.Id == projectId);
        if (!projectExists) return NotFound("Project not found.");

        var query = _db.ProjectCcfRefs
            .Where(x => x.ProjectId == projectId && !x.IsDeleted);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var rows = await query
            .OrderBy(x => x.Code)
            .Select(x => new ProjectCcfRefDto(x.Id, x.Code, x.IsActive))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("{projectId:int}/ccf-refs")]
    public async Task<IActionResult> CreateProjectCcfRef([FromRoute] int projectId, [FromBody] CreateProjectCcfRefDto dto)
    {
        var projectExists = await _db.Projects.AnyAsync(p => p.Id == projectId);
        if (!projectExists) return NotFound("Project not found.");

        string normalized;
        try
        {
            normalized = NormalizeCcfRef(dto.Code);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var existing = await _db.ProjectCcfRefs
            .FirstOrDefaultAsync(x => x.ProjectId == projectId && x.Code == normalized);

        if (existing != null)
        {
            if (existing.IsDeleted)
                return BadRequest("CCF Ref is deleted.");

            var wasInactive = !existing.IsActive;

            if (wasInactive)
            {
                existing.IsActive = true;
                await _db.SaveChangesAsync();
            }

            var a = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a.WorkerId,
                subjectWorkerId: a.WorkerId,
                actorRole: a.Role,
                actorSource: a.Source,
                action: wasInactive ? "Projects.CcfRef.Reactivate" : "Projects.CcfRef.CreateNoop",
                entityType: "ProjectCcfRef",
                entityId: existing.Id.ToString(),
                summary: $"ProjectId={projectId}, CcfRef={existing.Code}, WasInactive={wasInactive}"
            );

            return Ok(new ProjectCcfRefDto(existing.Id, existing.Code, existing.IsActive));
        }

        var created = new ProjectCcfRef
        {
            ProjectId = projectId,
            Code = normalized,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProjectCcfRefs.Add(created);
        await _db.SaveChangesAsync();

        var a2 = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a2.WorkerId,
            subjectWorkerId: a2.WorkerId,
            actorRole: a2.Role,
            actorSource: a2.Source,
            action: "Projects.CcfRef.Create",
            entityType: "ProjectCcfRef",
            entityId: created.Id.ToString(),
            summary: $"ProjectId={projectId}, CcfRef={created.Code}, IsActive={created.IsActive}"
        );

        return Ok(new ProjectCcfRefDto(created.Id, created.Code, created.IsActive));
    }

    // Minimal local helper (same rules as TimesheetController)
    private static string NormalizeCcfRef(string input)
    {
        var raw = (input ?? "").Trim();

        if (raw.Length == 0)
            throw new InvalidOperationException("CCF Ref is required.");

        for (int i = 0; i < raw.Length; i++)
        {
            var ch = raw[i];
            if (ch < '0' || ch > '9')
                throw new InvalidOperationException("CCF Ref must be numeric.");
        }

        if (!int.TryParse(raw, out var n))
            throw new InvalidOperationException("Invalid CCF Ref.");

        if (n == 0)
            throw new InvalidOperationException("CCF Ref 000 is not allowed.");

        if (n < 1 || n > 999)
            throw new InvalidOperationException("CCF Ref must be between 001 and 999.");

        return n.ToString("D3");
    }

    private static ProjectCcfRefDetailsDto ToDetailsDto(ProjectCcfRef x) =>
    new ProjectCcfRefDetailsDto(
        x.Id,
        x.Code,
        x.IsActive,
        x.EstimatedValue,
        x.QuotedValue,
        x.QuotedDateUtc,
        x.AgreedValue,
        x.AgreedDateUtc,
        x.ActualValue,
        x.Status,
        x.Notes
    );

    [HttpPatch("{projectId:int}/ccf-refs/{id:int}")]
    public async Task<IActionResult> SetProjectCcfRefActive([FromRoute] int projectId, [FromRoute] int id, [FromBody] bool isActive)
    {
        var row = await _db.ProjectCcfRefs.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == projectId);
        if (row == null) return NotFound();

        if (row.IsDeleted) return BadRequest("CCF Ref is deleted.");

        row.IsActive = isActive;
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.CcfRef.SetActive",
            entityType: "ProjectCcfRef",
            entityId: row.Id.ToString(),
            summary: $"ProjectId={projectId}, CcfRef={row.Code}, IsActive={isActive}"
        );

        return Ok(new ProjectCcfRefDto(row.Id, row.Code, row.IsActive));
    }

    [HttpGet("{jobKey}/ccf-refs")]
    public async Task<IActionResult> GetProjectCcfRefsByJobKey(
    [FromRoute] string jobKey,
    [FromQuery] bool includeInactive = false)
    {
        jobKey = (jobKey ?? "").Trim();
        if (jobKey.Length == 0) return BadRequest("JobKey is required.");

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);

        if (project == null) return NotFound("Project not found.");

        var query = _db.ProjectCcfRefs
            .Where(x => x.ProjectId == project.Id && !x.IsDeleted);

        if (!includeInactive)
            query = query.Where(x => x.IsActive);

        var rows = await query
            .OrderBy(x => x.Code)
            .Select(x => new ProjectCcfRefDetailsDto(
                x.Id,
                x.Code,
                x.IsActive,
                x.EstimatedValue,
                x.QuotedValue,
                x.QuotedDateUtc,
                x.AgreedValue,
                x.AgreedDateUtc,
                x.ActualValue,
                x.Status,
                x.Notes
            ))
            .ToListAsync();

        return Ok(rows);
    }

    [HttpPost("{jobKey}/ccf-refs")]
    public async Task<IActionResult> CreateProjectCcfRefByJobKey(
        [FromRoute] string jobKey,
        [FromBody] CreateProjectCcfRefDto dto)
    {
        jobKey = (jobKey ?? "").Trim();
        if (jobKey.Length == 0) return BadRequest("JobKey is required.");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound("Project not found.");

        string normalized;
        try
        {
            normalized = NormalizeCcfRef(dto.Code);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(ex.Message);
        }

        var existing = await _db.ProjectCcfRefs
            .FirstOrDefaultAsync(x => x.ProjectId == project.Id && x.Code == normalized);

        if (existing != null)
        {
            if (existing.IsDeleted)
                return BadRequest("CCF Ref is deleted.");

            var wasInactive = !existing.IsActive;

            if (wasInactive)
            {
                existing.IsActive = true;
                await _db.SaveChangesAsync();
            }

            var a = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a.WorkerId,
                subjectWorkerId: a.WorkerId,
                actorRole: a.Role,
                actorSource: a.Source,
                action: wasInactive ? "Projects.CcfRef.Reactivate" : "Projects.CcfRef.CreateNoop",
                entityType: "ProjectCcfRef",
                entityId: existing.Id.ToString(),
                summary: $"Job={jobKey}, CcfRef={existing.Code}, WasInactive={wasInactive}"
            );

            return Ok(ToDetailsDto(existing));
        }

        var created = new ProjectCcfRef
        {
            ProjectId = project.Id,
            Code = normalized,
            IsActive = true,
            CreatedAtUtc = DateTime.UtcNow
        };

        _db.ProjectCcfRefs.Add(created);
        await _db.SaveChangesAsync();

        var a2 = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a2.WorkerId,
            subjectWorkerId: a2.WorkerId,
            actorRole: a2.Role,
            actorSource: a2.Source,
            action: "Projects.CcfRef.Create",
            entityType: "ProjectCcfRef",
            entityId: created.Id.ToString(),
            summary: $"Job={jobKey}, CcfRef={created.Code}, IsActive={created.IsActive}"
        );

        return Ok(ToDetailsDto(created));
    }

    [HttpPatch("{jobKey}/ccf-refs/{id:int}")]
    public async Task<IActionResult> SetProjectCcfRefActiveByJobKey(
        [FromRoute] string jobKey,
        [FromRoute] int id,
        [FromBody] bool isActive)
    {
        jobKey = (jobKey ?? "").Trim();
        if (jobKey.Length == 0) return BadRequest("JobKey is required.");

        var project = await _db.Projects.AsNoTracking()
            .FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);

        if (project == null) return NotFound("Project not found.");

        var row = await _db.ProjectCcfRefs
            .FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == project.Id);

        if (row == null) return NotFound();

        if (row.IsDeleted) return BadRequest("CCF Ref is deleted.");

        row.IsActive = isActive;
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.CcfRef.SetActive",
            entityType: "ProjectCcfRef",
            entityId: row.Id.ToString(),
            summary: $"Job={jobKey}, CcfRef={row.Code}, IsActive={isActive}"
        );

        return Ok(ToDetailsDto(row));
    }

    [HttpPut("{jobKey}/ccf-refs/{id:int}")]
    public async Task<IActionResult> UpdateProjectCcfRefByJobKey(
    [FromRoute] string jobKey,
    [FromRoute] int id,
    [FromBody] UpdateProjectCcfRefDto dto)
    {
        jobKey = (jobKey ?? "").Trim();
        if (jobKey.Length == 0) return BadRequest("JobKey is required.");

        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound("Project not found.");

        var row = await _db.ProjectCcfRefs.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == project.Id);
        if (row == null) return NotFound("CCF Ref not found.");

        if (row.IsDeleted) return BadRequest("CCF Ref is deleted.");

        // Apply updates
        row.EstimatedValue = dto.EstimatedValue;
        row.QuotedValue = dto.QuotedValue;
        row.QuotedDateUtc = AsUtcDateOrNull(dto.QuotedDateUtc);
        row.AgreedValue = dto.AgreedValue;
        row.AgreedDateUtc = AsUtcDateOrNull(dto.AgreedDateUtc);
        row.ActualValue = dto.ActualValue;
        row.Status = dto.Status ?? "";
        row.Notes = dto.Notes;

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            subjectWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Projects.CcfRef.Update",
            entityType: "ProjectCcfRef",
            entityId: row.Id.ToString(),
            summary: $"Job={jobKey}, CcfRef={row.Code}, Est={row.EstimatedValue}, Quoted={row.QuotedValue}, QuotedDate={(row.QuotedDateUtc?.ToString("yyyy-MM-dd") ?? "null")}, Agreed={row.AgreedValue}, AgreedDate={(row.AgreedDateUtc?.ToString("yyyy-MM-dd") ?? "null")}, Actual={row.ActualValue}, Status={row.Status}"
        );

        return Ok(ToDetailsDto(row));
    }

    [HttpPatch("{jobKey}/ccf-refs/{id:int}/deleted")]
    public async Task<IActionResult> SetProjectCcfRefDeletedByJobKey(
    [FromRoute] string jobKey,
    [FromRoute] int id,
    [FromBody] bool isDeleted)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(x => x.JobNameOrNumber == jobKey);
        if (project == null) return NotFound($"Project not found: {jobKey}");

        var row = await _db.ProjectCcfRefs.FirstOrDefaultAsync(x => x.Id == id && x.ProjectId == project.Id);
        if (row == null) return NotFound("CCF Ref not found.");

        // If already deleted and asked to delete again, treat as ok/noop
        var wasDeleted = row.IsDeleted;

        if (isDeleted)
        {
            if (!row.IsDeleted)
            {
                row.IsDeleted = true;
                row.IsActive = false;
                row.DeletedAtUtc = DateTime.UtcNow;

                var a = GetActorForAudit();
                row.DeletedByWorkerId = a.WorkerId;

                await _db.SaveChangesAsync();

                await _audit.LogAsync(
                    actorWorkerId: a.WorkerId,
                    subjectWorkerId: a.WorkerId,
                    actorRole: a.Role,
                    actorSource: a.Source,
                    action: "Projects.CcfRef.Delete",
                    entityType: "ProjectCcfRef",
                    entityId: row.Id.ToString(),
                    summary: $"Job={jobKey}, CcfRef={row.Code}"
                );
            }

            return Ok();
        }

        // Restore (not exposed in UI yet)
        if (row.IsDeleted)
        {
            row.IsDeleted = false;
            row.DeletedAtUtc = null;
            row.DeletedByWorkerId = null;

            await _db.SaveChangesAsync();

            var a2 = GetActorForAudit();
            await _audit.LogAsync(
                actorWorkerId: a2.WorkerId,
                subjectWorkerId: a2.WorkerId,
                actorRole: a2.Role,
                actorSource: a2.Source,
                action: "Projects.CcfRef.Restore",
                entityType: "ProjectCcfRef",
                entityId: row.Id.ToString(),
                summary: $"Job={jobKey}, CcfRef={row.Code}"
            );
        }

        return Ok();
    }

    [HttpGet("{jobKey}/edit")]
    public async Task<ActionResult<ProjectEditDto>> GetForEdit(string jobKey)
    {
        jobKey = (jobKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jobKey))
            return BadRequest("Job key is required.");

        var p = await _db.Projects
            .AsNoTracking()
            .Include(x => x.CompanyEntity)
            .FirstOrDefaultAsync(x => x.IsRealProject && x.JobNameOrNumber == jobKey);

        if (p == null)
            return NotFound("Project not found.");

        return new ProjectEditDto
        {
            JobNameOrNumber = p.JobNameOrNumber,
            CompanyId = p.CompanyId,
            CompanyCode = p.CompanyEntity != null ? p.CompanyEntity.Code : null,
            CompanyName = p.CompanyEntity != null ? p.CompanyEntity.Name : null,
            IsActive = p.IsActive
        };
    }

    [HttpPut("{jobKey}")]
    public async Task<IActionResult> Update(string jobKey, [FromBody] UpdateProjectRequestDto dto)
    {
        jobKey = (jobKey ?? "").Trim();
        if (string.IsNullOrWhiteSpace(jobKey))
            return BadRequest("Job key is required.");

        var p = await _db.Projects
            .Include(x => x.CompanyEntity)
            .FirstOrDefaultAsync(x => x.IsRealProject && x.JobNameOrNumber == jobKey);

        if (p == null)
            return NotFound("Project not found.");

        // Validate company if provided
        Company? company = null;
        if (dto.CompanyId.HasValue)
        {
            company = await _db.Companies.FirstOrDefaultAsync(c => c.Id == dto.CompanyId.Value);
            if (company == null)
                return BadRequest("Company not found.");
        }

        // Apply changes (v1: job key is immutable)
        p.CompanyId = dto.CompanyId;
        p.IsActive = dto.IsActive;

        await _db.SaveChangesAsync();

        // Audit (minimal)
        var actor = GetActorForAudit();
        await _audit.LogAsync(
            actor.WorkerId,
            actor.WorkerId,
            actor.Role,
            actor.Source,
            action: "Project.Update",
            entityType: "Project",
            entityId: p.Id.ToString(),
            summary: $"Updated project {p.JobNameOrNumber}: CompanyId={p.CompanyId}, IsActive={p.IsActive}");

        return NoContent();
    }

    private static decimal? DerivePaymentNet(
    decimal? paymentGross,
    decimal? vatRate,
    decimal? invoiceNet,
    decimal? invoiceGross)
    {
        if (!paymentGross.HasValue)
            return null;

        // Prefer VAT rate if present (0.20m etc.)
        if (vatRate.HasValue)
        {
            var divisor = 1m + vatRate.Value;
            if (divisor <= 0m) return paymentGross; // safety
            return Math.Round(paymentGross.Value / divisor, 2, MidpointRounding.AwayFromZero);
        }

        // Fallback: derive multiplier from gross/net if available
        if (invoiceNet.HasValue && invoiceGross.HasValue && invoiceGross.Value != 0m)
        {
            var ratio = invoiceNet.Value / invoiceGross.Value; // net = gross * ratio
            return Math.Round(paymentGross.Value * ratio, 2, MidpointRounding.AwayFromZero);
        }

        // Final fallback: assume no VAT
        return Math.Round(paymentGross.Value, 2, MidpointRounding.AwayFromZero);
    }

}
