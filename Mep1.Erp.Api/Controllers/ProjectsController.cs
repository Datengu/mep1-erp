using Mep1.Erp.Application;
using Mep1.Erp.Core;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly AppDbContext _db;

    public ProjectsController(AppDbContext db)
    {
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> Get()
    {
        var projects = await _db.Projects
            .AsNoTracking()
            .OrderBy(p => p.JobNameOrNumber)
            .Select(p => new
            {
                p.Id,
                p.JobNameOrNumber,
                p.Company,
                p.IsActive
            })
            .ToListAsync();

        return Ok(projects);
    }

    [HttpGet("summary")]
    public ActionResult<List<ProjectSummaryDto>> GetSummary()
    {
        var rows = Reporting.GetProjectCostVsInvoiced(_db);

        var dto = rows.Select(p => new ProjectSummaryDto
        {
            JobNameOrNumber = p.JobNameOrNumber,
            BaseCode = p.BaseCode,
            LabourCost = p.LabourCost,
            SupplierCost = p.SupplierCost,
            TotalCost = p.TotalCost,
            InvoicedNet = p.InvoicedNet,
            InvoicedGross = p.InvoicedGross,
            ProfitNet = p.ProfitNet,
            ProfitGross = p.ProfitGross
        }).ToList();

        return Ok(dto);
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

        var today = DateTime.Today.Date;
        var monthStart = new DateTime(today.Year, today.Month, 1);

        var labourThisMonth = Reporting.GetProjectLabourByPerson(_db, project.Id, monthStart, today)
            .Select(x => new ProjectLabourByPersonRowDto(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
            .ToList();

        var labourAllTime = Reporting.GetProjectLabourByPerson(_db, project.Id)
            .Select(x => new ProjectLabourByPersonRowDto(x.WorkerInitials, x.WorkerName, x.Hours, x.Cost))
            .ToList();

        var recentEntries = Reporting.GetProjectRecentEntries(_db, project.Id, take: recentTake)
            .Select(x => new ProjectRecentEntryRowDto(x.Date, x.WorkerInitials, x.Hours, x.Cost, x.TaskDescription))
            .ToList();

        var invoices = Reporting.GetProjectInvoiceRows(_db, baseCode)
            .Select(x => new ProjectInvoiceRowDto(x.InvoiceNumber, x.InvoiceDate, x.DueDate, x.NetAmount, x.OutstandingNet, x.Status))
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
            Date = dto.Date?.Date,
            Amount = dto.Amount,
            Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim()
        };

        _db.SupplierCosts.Add(entity);
        await _db.SaveChangesAsync();

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
        entity.Date = dto.Date?.Date;
        entity.Amount = dto.Amount;
        entity.Note = string.IsNullOrWhiteSpace(dto.Note) ? null : dto.Note.Trim();

        await _db.SaveChangesAsync();
        return NoContent();
    }

    [HttpDelete("{jobKey}/supplier-costs/{id:int}")]
    public async Task<IActionResult> DeleteSupplierCost([FromRoute] string jobKey, [FromRoute] int id)
    {
        var project = await _db.Projects.FirstOrDefaultAsync(p => p.JobNameOrNumber == jobKey);
        if (project == null) return NotFound();

        var entity = await _db.SupplierCosts.FirstOrDefaultAsync(sc => sc.Id == id && sc.ProjectId == project.Id);
        if (entity == null) return NotFound();

        _db.SupplierCosts.Remove(entity);
        await _db.SaveChangesAsync();

        return NoContent();
    }

}
