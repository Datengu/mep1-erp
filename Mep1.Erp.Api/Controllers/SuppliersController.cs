using Mep1.Erp.Core;
using Mep1.Erp.Core.Contracts;
using Mep1.Erp.Infrastructure;
using Mep1.Erp.Api.Services;
using Mep1.Erp.Api.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Mep1.Erp.Api.Controllers;

[ApiController]
[Route("api/suppliers")]
[Authorize(Policy = "AdminOrOwner")]
public class SuppliersController : ControllerBase
{
    private readonly AppDbContext _db;

    private readonly AuditLogger _audit;

    public SuppliersController(AppDbContext db, AuditLogger audit)
    {
        _db = db;
        _audit = audit;
    }

    private bool IsAdminKey()
    => string.Equals(HttpContext.Items["ApiKeyKind"] as string, "Admin", StringComparison.Ordinal);

    private ActionResult? RequireAdminKey()
    {
        if (IsAdminKey()) return null;
        return Unauthorized("Admin API key required.");
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

    [HttpGet]
    public async Task<ActionResult<List<SupplierDto>>> GetAll([FromQuery] bool includeInactive = false)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

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
        var guard = RequireAdminKey();
        if (guard != null) return guard;

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

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Suppliers.Create",
            entityType: "Supplier",
            entityId: entity.Id.ToString(),
            summary: $"Name={entity.Name}, Active={entity.IsActive}"
        );

        return Ok(new SupplierDto(entity.Id, entity.Name, entity.IsActive, entity.Notes));
    }

    [HttpPut("{id:int}")]
    public async Task<IActionResult> Update([FromRoute] int id, [FromBody] UpsertSupplierDto dto)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        var name = (dto.Name ?? "").Trim();
        if (name.Length == 0) return BadRequest("Name is required.");

        entity.Name = name;
        entity.IsActive = dto.IsActive;
        entity.Notes = string.IsNullOrWhiteSpace(dto.Notes) ? null : dto.Notes.Trim();

        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Suppliers.Update",
            entityType: "Supplier",
            entityId: entity.Id.ToString(),
            summary: $"Name={entity.Name}, Active={entity.IsActive}"
        );

        return NoContent();
    }

    [HttpPost("{id:int}/deactivate")]
    public async Task<IActionResult> Deactivate([FromRoute] int id)
    {
        var guard = RequireAdminKey();
        if (guard != null) return guard;

        var entity = await _db.Suppliers.FirstOrDefaultAsync(s => s.Id == id);
        if (entity == null) return NotFound();

        entity.IsActive = false;
        await _db.SaveChangesAsync();

        var a = GetActorForAudit();
        await _audit.LogAsync(
            actorWorkerId: a.WorkerId,
            actorRole: a.Role,
            actorSource: a.Source,
            action: "Suppliers.Deactivate",
            entityType: "Supplier",
            entityId: entity.Id.ToString(),
            summary: $"Name={entity.Name}"
        );

        return NoContent();
    }
}
