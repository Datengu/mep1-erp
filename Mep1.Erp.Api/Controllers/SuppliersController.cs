using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Mep1.Erp.Core;
using Mep1.Erp.Infrastructure;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;
    public SuppliersController(AppDbContext db) => _db = db;

    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var q = _db.Suppliers.AsNoTracking();

        if (!includeInactive)
            q = q.Where(s => s.IsActive);

        var items = await q
            .OrderBy(s => s.Name)
            .Select(s => new SupplierDto(s.Id, s.Name, s.IsActive, s.Notes))
            .ToListAsync();

        return Ok(items);
    }

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create([FromBody] UpsertSupplierDto dto)
    {
        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Name is required.");

        var entity = new Supplier
        {
            Name = name,
            IsActive = dto.IsActive,
            Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim()
        };

        _db.Suppliers.Add(entity);

        await _db.SaveChangesAsync();

        return Ok(new SupplierDto(entity.Id, entity.Name, entity.IsActive, entity.Notes));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpsertSupplierDto dto)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Name is required.");

        entity.Name = name;
        entity.IsActive = dto.IsActive;
        entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await _db.SaveChangesAsync();

        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] int id)
    {
        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        entity.IsActive = false;
        await _db.SaveChangesAsync();

        return NoContent();
    }
}
